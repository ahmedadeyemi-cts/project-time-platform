using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 076 defines the ProjectPulse defect intake, tracking, assignment,
/// notification, and GitHub synchronization contract. This source checkpoint
/// is deliberately fail-closed: it reads existing ProjectPulse identity and
/// authorization data, but it does not create a defect table, write an outbox
/// record, call GitHub, invoke an AI provider, or send email.
/// </summary>
public static class DefectTrackerModule
{
    private const string ModuleNumber = "076";
    private const string ContractVersion = "2026-07-20.1";
    private const string ImplementationBaseline =
        "3d9a3dca8af479c854dc4c4a9294bc8aad273074";
    private const string DefaultAhmedEmail = "ahmed.adeyemi@ussignal.com";

    public static WebApplication MapDefectTrackerEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/defect-tracker/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet(
            "/api/defect-tracker/defects",
            (Func<HttpContext, Task<IResult>>)GetDefectsAsync);
        app.MapGet(
            "/api/defect-tracker/assignee-options",
            (Func<HttpContext, Task<IResult>>)GetAssigneeOptionsAsync);
        app.MapGet(
            "/api/defect-tracker/intake-policy",
            (Func<HttpContext, Task<IResult>>)GetIntakePolicyAsync);
        app.MapGet(
            "/api/defect-tracker/notification-policy",
            (Func<HttpContext, Task<IResult>>)GetNotificationPolicyAsync);
        app.MapGet(
            "/api/defect-tracker/integration-policy",
            (Func<HttpContext, Task<IResult>>)GetIntegrationPolicyAsync);

        // Mutation-shaped routes are registered as discoverable contracts.
        // They stop before reading a body or changing internal/external state.
        app.MapPost(
            "/api/defect-tracker/report",
            (Func<HttpContext, Task<IResult>>)LockedReportAsync);
        app.MapMethods(
            "/api/defect-tracker/defects/{defectId}",
            [HttpMethods.Patch],
            (Func<HttpContext, Task<IResult>>)LockedUpdateAsync);
        app.MapPost(
            "/api/defect-tracker/defects/{defectId}/reassign",
            (Func<HttpContext, Task<IResult>>)LockedReassignAsync);
        app.MapPost(
            "/api/defect-tracker/defects/{defectId}/comments",
            (Func<HttpContext, Task<IResult>>)LockedCommentAsync);
        app.MapPost(
            "/api/defect-tracker/defects/{defectId}/resolve",
            (Func<HttpContext, Task<IResult>>)LockedResolveAsync);
        app.MapPost(
            "/api/defect-tracker/integrations/github/events",
            (Func<HttpContext, Task<IResult>>)LockedGitHubIntakeAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var defaultAssignee = await ResolveDefaultAssigneeAsync(connection);

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Defect Intake & Resolution Tracker",
            status = "defect_tracker_contract_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            generatedAt = DateTimeOffset.UtcNow,
            runtimeEnvironment = RuntimeEnvironment(),
            access = AccessResponse(authorization.Access!, context, defaultAssignee),
            summary = new
            {
                total = 0,
                open = 0,
                inProgress = 0,
                blocked = 0,
                resolved = 0,
                critical = 0,
                inventoryState = "durable_defect_store_not_authorized"
            },
            defaultAssignee,
            idPolicy = DefectIdPolicy(),
            lifecycle = Statuses(),
            priorities = Priorities(),
            categories = Categories(),
            sourceChannels = SourceChannels(),
            persistence = PersistenceBoundary(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetDefectsAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var defaultAssignee = await ResolveDefaultAssigneeAsync(connection);
        var access = authorization.Access!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "defect_inventory_contract_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            scope = IsViewAs(context)
                ? "effective_user_reported_or_assigned_defects"
                : access.CanViewAllDefects
                    ? "all_defects"
                    : "own_reported_or_assigned_defects",
            inventoryState = "durable_defect_store_not_authorized",
            defects = Array.Empty<object>(),
            columns = DefectColumns(),
            defaultAssignee,
            statement = "No durable Module 076 defect repository is connected, so this endpoint does not represent an empty production defect inventory.",
            pagination = new { supported = true, defaultPageSize = 50, maximumPageSize = 200 },
            filters = new[]
            {
                "status", "category", "priority", "assigneeUserId",
                "raisedByUserId", "sourceChannel", "affectedModule",
                "dateAddedFrom", "dateAddedTo", "githubIssueNumber"
            }
        });
    }

    private static async Task<IResult> GetAssigneeOptionsAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var defaultAssignee = await ResolveDefaultAssigneeAsync(connection);
        var access = authorization.Access!;

        if (!access.CanReassign(defaultAssignee))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "defect_reassignment_authority_required",
                permission = "MANAGE_DEFECTS",
                message = "Defect reassignment is restricted to the default owner and authorized management roles."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    u.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                    u.email,
                    COALESCE(NULLIF(u.job_title, ''), '') AS job_title,
                    COALESCE(
                        NULLIF(u.team_name, ''),
                        NULLIF(u.department_name, ''),
                        NULLIF(u.department, ''),
                        'Unassigned') AS team_name
                FROM app_users u
                WHERE u.is_active = TRUE
                  AND COALESCE(u.login_enabled, TRUE) = TRUE
                ORDER BY display_name, u.email
                LIMIT 500;
                """, connection);

            var identities = new List<object>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                identities.Add(new
                {
                    userId = reader.GetGuid(0),
                    displayName = reader.GetString(1),
                    email = reader.GetString(2),
                    jobTitle = reader.GetString(3),
                    teamName = reader.GetString(4),
                    isDefaultAssignee = defaultAssignee.UserId == reader.GetGuid(0)
                });
            }

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "defect_assignee_options_loaded",
                identityAuthority = "Module 062 / app_users.user_id",
                defaultAssignee,
                count = identities.Count,
                identities
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load defect assignee options");
            return DependencyUnavailable("Defect assignee choices are temporarily unavailable.");
        }
    }

    private static async Task<IResult> GetIntakePolicyAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var defaultAssignee = await ResolveDefaultAssigneeAsync(connection);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "defect_intake_policy_loaded",
            contractVersion = ContractVersion,
            defaultAssignee,
            idPolicy = DefectIdPolicy(),
            requiredFields = new[]
            {
                "title", "description", "category", "priority",
                "raisedByUserId", "sourceChannel", "dateAdded"
            },
            optionalFields = new[]
            {
                "affectedModule", "affectedRoute", "environment",
                "reproductionSteps", "expectedBehavior", "actualBehavior",
                "githubRepository", "githubIssueNumber", "githubIssueUrl",
                "initialComment"
            },
            channels = SourceChannels(),
            datePolicy = new
            {
                dateAdded = "Assigned by the server in UTC when durable creation succeeds.",
                dateResolved = "Assigned by the server in UTC on the first Resolved or Closed transition.",
                reopened = "A reopen clears the current resolution timestamp while preserving the prior transition in append-only history.",
                resolutionTime = "Calculated by the server as dateResolved minus dateAdded; clients do not submit it."
            },
            validation = new
            {
                titleMaximumCharacters = 180,
                descriptionMaximumCharacters = 8000,
                commentMaximumCharacters = 4000,
                attachmentsEnabled = false,
                secretOrCredentialContentProhibited = true
            },
            durableCreationEnabled = false
        });
    }

    private static async Task<IResult> GetNotificationPolicyAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "defect_notification_policy_loaded",
            contractVersion = ContractVersion,
            owner = "Module 067 Global Mail Configuration",
            provider = "shared ProjectPulse global mail only",
            events = new[]
            {
                new
                {
                    eventCode = "defect_opened",
                    recipients = "active manager role group",
                    timing = "after the defect and outbox event commit atomically",
                    deduplicationKey = "defect_opened:{defectId}",
                    includes = new[] { "defect ID", "priority", "category", "summary", "reporter", "assignee", "link" }
                },
                new
                {
                    eventCode = "defect_resolved",
                    recipients = "original reporter",
                    timing = "after the resolved transition and outbox event commit atomically",
                    deduplicationKey = "defect_resolved:{defectId}:{resolutionVersion}",
                    includes = new[] { "defect ID", "resolution summary", "date resolved", "resolution time", "link" }
                }
            },
            managerRoles = ManagerRoleCodes(),
            controls = new
            {
                outboxWriteEnabled = false,
                deliveryEnabled = false,
                directSmtpClientPresent = false,
                directBrevoClientPresent = false,
                externalNotificationSent = false,
                reason = "Database persistence and Global Mail delivery require separate authorization."
            }
        });
    }

    private static async Task<IResult> GetIntegrationPolicyAsync(HttpContext context)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "defect_integration_policy_loaded",
            contractVersion = ContractVersion,
            repository = "ahmedadeyemi-cts/project-time-platform",
            integrations = new[]
            {
                new { channel = "help", state = "source_connected", mechanism = "ProjectPulse Help opens the Module 076 intake route." },
                new { channel = "github", state = "issue_form_present_webhook_locked", mechanism = "Governed GitHub issue form plus future signed webhook." },
                new { channel = "claude_github", state = "contract_ready_webhook_locked", mechanism = "Claude reports through a GitHub issue; Module 076 performs no direct Claude call." },
                new { channel = "chatgpt_github", state = "contract_ready_webhook_locked", mechanism = "ChatGPT reports through a GitHub issue; Module 076 performs no direct OpenAI call." }
            },
            github = new
            {
                requiredRepositoryAllowlist = new[] { "ahmedadeyemi-cts/project-time-platform" },
                requiredEvents = new[] { "issues.opened", "issues.edited", "issues.closed", "issues.reopened", "issue_comment.created", "assigned", "unassigned" },
                requiredControls = new[]
                {
                    "GitHub App or webhook secret from an approved secret store",
                    "constant-time signature validation before body processing",
                    "delivery-ID deduplication",
                    "repository and installation allowlist",
                    "actor and source-channel attribution",
                    "rate limit and bounded payload",
                    "sanitized immutable audit evidence"
                },
                webhookEnabled = false,
                githubMutationEnabled = false,
                issueCreated = false,
                issueUpdated = false
            },
            ai = new
            {
                directClaudeExecutionEnabled = false,
                directOpenAiExecutionEnabled = false,
                sharedModule064RequiredForFutureTriage = true,
                sourceAttributionDerivedFromTrustedGitHubActorMetadata = true,
                arbitraryIssueTextCannotClaimTrustedAiOrigin = true
            }
        });
    }

    private static Task<IResult> LockedReportAsync(HttpContext context) =>
        LockedSessionOperationAsync(context, "report", null);

    private static Task<IResult> LockedUpdateAsync(HttpContext context) =>
        LockedSessionOperationAsync(context, "update", RouteDefectId(context));

    private static Task<IResult> LockedReassignAsync(HttpContext context) =>
        LockedSessionOperationAsync(context, "reassign", RouteDefectId(context));

    private static Task<IResult> LockedCommentAsync(HttpContext context) =>
        LockedSessionOperationAsync(context, "comment", RouteDefectId(context));

    private static Task<IResult> LockedResolveAsync(HttpContext context) =>
        LockedSessionOperationAsync(context, "resolve", RouteDefectId(context));

    private static string? RouteDefectId(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("defectId", out var value)
            ? value?.ToString()
            : null;

    private static async Task<IResult> LockedSessionOperationAsync(
        HttpContext context,
        string operation,
        string? defectId)
    {
        var authorization = await OpenAccessAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var defaultAssignee = await ResolveDefaultAssigneeAsync(connection);
        var access = authorization.Access!;

        if (IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                operation,
                defectId,
                requestBodyRead = false,
                stateChanged = false,
                message = "Exit Administrator View-As before performing a defect action."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (operation == "reassign" && !access.CanReassign(defaultAssignee))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "defect_reassignment_authority_required",
                permission = "MANAGE_DEFECTS",
                message = "Defect reassignment is restricted to the default owner and authorized management roles."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(new
        {
            module = ModuleNumber,
            status = "defect_operation_locked",
            operation,
            defectId,
            contractVersion = ContractVersion,
            requestBodyRead = false,
            durableDefectWritten = false,
            defectIdAllocated = false,
            assignmentChanged = false,
            commentWritten = false,
            resolutionRecorded = false,
            outboxEventWritten = false,
            emailSent = false,
            githubChanged = false,
            aiExecuted = false,
            stateChanged = false,
            persistence = PersistenceBoundary(),
            message = "Module 076 durable writes remain fail-closed pending database and activation authorization."
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static Task<IResult> LockedGitHubIntakeAsync(HttpContext context) =>
        Task.FromResult<IResult>(Results.Json(new
        {
            module = ModuleNumber,
            status = "github_defect_intake_locked",
            contractVersion = ContractVersion,
            requestBodyRead = false,
            signatureValidated = false,
            deliveryRecorded = false,
            repositoryAllowlistChecked = false,
            defectCreated = false,
            defectUpdated = false,
            emailSent = false,
            githubChanged = false,
            aiExecuted = false,
            stateChanged = false,
            message = "GitHub defect intake requires a separately authorized signed-webhook adapter and durable idempotency store."
        }, statusCode: StatusCodes.Status423Locked));

    private static object DefectIdPolicy() => new
    {
        format = "DEF-{YYYY}-{SEQUENCE:000000}",
        example = "DEF-2026-000001",
        allocation = "atomic server-side sequence inside the durable create transaction",
        clientSuppliedIdsAccepted = false,
        previewIdsAreOfficial = false
    };

    private static string[] DefectColumns() =>
    [
        "defectId", "status", "description", "category", "priority",
        "assignee", "raisedBy", "sourceChannel", "affectedModule",
        "dateAdded", "dateResolved", "resolutionTime", "comments",
        "githubIssue"
    ];

    private static string[] Statuses() =>
    ["Open", "In Progress", "Blocked", "Resolved", "Closed", "Reopened"];

    private static string[] Priorities() =>
    ["Critical", "High", "Medium", "Low"];

    private static string[] Categories() =>
    [
        "Bug", "Regression", "User Interface", "API", "Authentication",
        "Authorization", "Data", "Integration", "Performance",
        "Documentation", "Feature Gap", "Other"
    ];

    private static object[] SourceChannels() =>
    [
        new { code = "help", label = "ProjectPulse Help", trusted = true },
        new { code = "tracker", label = "Module 076 Tracker", trusted = true },
        new { code = "github", label = "GitHub", trusted = true },
        new { code = "claude_github", label = "Claude through GitHub", trusted = false },
        new { code = "chatgpt_github", label = "ChatGPT through GitHub", trusted = false }
    ];

    private static object PersistenceBoundary() => new
    {
        durableStoreConfigured = false,
        schemaAuthorized = false,
        repositoryAdapterConfigured = false,
        writesEnabled = false,
        notificationOutboxEnabled = false,
        githubWebhookEnabled = false,
        activationState = "complete_source_fail_closed"
    };

    private static string[] ManagerRoleCodes() =>
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "MANAGER",
        "ENGINEERING_MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
        "PROJECT_TEAM_COORDINATOR"
    ];

    private static string[] Guardrails() =>
    [
        "Every authenticated user can reach the intake surface; future reads are server-scoped to own reported/assigned defects unless management authority is present.",
        "The configured Ahmed identity is the default assignee and remains backed by Module 062 stable app_users.user_id when resolved.",
        "View-As never grants report, update, reassign, comment, resolve, or webhook authority.",
        "Defect IDs, date added, date resolved, and resolution time are server-calculated only.",
        "Open notifications target the active manager role group; resolution notifications target the original reporter.",
        "All mail must use Module 067 and a transactional outbox; Module 076 has no direct mail provider.",
        "Claude and ChatGPT enter through trusted GitHub metadata only; Module 076 performs no direct AI execution.",
        "No request body is read by a locked operation and no database, GitHub, AI, or email state changes.",
        "Modules 002, 056E, 059, 062, and 064-074 remain preserved."
    ];

    private static async Task<DefaultAssignee> ResolveDefaultAssigneeAsync(
        NpgsqlConnection connection)
    {
        var configuredEmail = Environment.GetEnvironmentVariable(
            "PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL");
        var email = string.IsNullOrWhiteSpace(configuredEmail)
            ? DefaultAhmedEmail
            : configuredEmail.Trim();

        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email),
                u.email
            FROM app_users u
            WHERE u.is_active = TRUE
              AND lower(u.email) = lower(@email)
            ORDER BY u.user_id
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("email", email);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new DefaultAssignee(
                null,
                "Ahmed Adeyemi",
                email,
                "identity_resolution_required",
                "PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL");
        }

        return new DefaultAssignee(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            "resolved_from_module_062_identity",
            "PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL");
    }

    private static async Task<AccessOutcome> OpenAccessAsync(HttpContext context)
    {
        var actualUserId = SessionUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId");

        if (actualUserId is null || effectiveUserId is null)
        {
            return new AccessOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new AccessOutcome(
                null,
                null,
                DependencyUnavailable("Defect Tracker authorization is temporarily unavailable."));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    u.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email),
                    u.email,
                    COALESCE(
                        string_agg(DISTINCT upper(r.role_code), ',')
                            FILTER (WHERE r.role_code IS NOT NULL),
                        ''),
                    COALESCE(
                        string_agg(DISTINCT upper(p.permission_code), ',')
                            FILTER (WHERE p.permission_code IS NOT NULL),
                        '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                  ON ura.user_id = u.user_id
                 AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp
                  ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p
                  ON p.app_permission_id = rp.app_permission_id
                WHERE u.user_id = @user_id
                  AND u.is_active = TRUE
                GROUP BY u.user_id, u.display_name, u.email;
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await connection.DisposeAsync();
                return new AccessOutcome(null, null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "active_user_required",
                    message = "An active ProjectPulse identity is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            var roles = SplitCodes(reader.GetString(3));
            var permissions = SplitCodes(reader.GetString(4));
            var access = new AccessContext(
                actualUserId.Value,
                effectiveUserId.Value,
                reader.GetString(1),
                reader.GetString(2),
                roles,
                permissions);

            return new AccessOutcome(connection, access, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            LogFailure(context, exception, "authorize defect tracker access");
            return new AccessOutcome(
                null,
                null,
                DependencyUnavailable("Defect Tracker authorization is temporarily unavailable."));
        }
    }

    private static object AccessResponse(
        AccessContext access,
        HttpContext context,
        DefaultAssignee defaultAssignee) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        displayName = access.DisplayName,
        roles = access.Roles.OrderBy(value => value),
        permissions = access.Permissions.OrderBy(value => value),
        canReport = !IsViewAs(context),
        canViewAllDefects = !IsViewAs(context) && access.CanViewAllDefects,
        canManage = !IsViewAs(context) && access.CanManage,
        canReassign = !IsViewAs(context) && access.CanReassign(defaultAssignee),
        canResolve = !IsViewAs(context)
            && (access.CanManage || access.CanReassign(defaultAssignee)),
        isViewAs = IsViewAs(context),
        authoritySource = "actual ProjectPulse session",
        viewAsTransfersMutationAuthority = false
    };

    private static HashSet<string> SplitCodes(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
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
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require
        }.ConnectionString;
    }

    private static string RuntimeEnvironment() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Unknown";

    private static IResult DependencyUnavailable(string message) => Results.Json(new
    {
        module = ModuleNumber,
        status = "authorization_dependency_unavailable",
        message
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static void LogFailure(
        HttpContext context,
        Exception exception,
        string operation)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DefectTrackerModule");
        logger.LogWarning(
            "Module 076 could not {Operation} ({ExceptionType}).",
            operation,
            exception.GetType().Name);
    }

    private sealed record AccessOutcome(
        NpgsqlConnection? Connection,
        AccessContext? Access,
        IResult? Failure);

    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string Email,
        HashSet<string> Roles,
        HashSet<string> Permissions)
    {
        public bool CanManage =>
            Roles.Overlaps(ManagerRoleCodes())
            || Permissions.Overlaps(new[]
            {
                "MANAGE_DEFECTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"
            });

        public bool CanViewAllDefects =>
            CanManage || Permissions.Contains("VIEW_ALL_DEFECTS");

        public bool CanReassign(DefaultAssignee defaultAssignee) =>
            CanManage
            || (defaultAssignee.UserId.HasValue
                && defaultAssignee.UserId.Value == ActualUserId)
            || string.Equals(
                Email,
                defaultAssignee.Email,
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DefaultAssignee(
        Guid? UserId,
        string DisplayName,
        string Email,
        string State,
        string ConfigurationSource);
}
