using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 068 publishes a versioned, administrator-only view of the
/// ProjectPulse system architecture. The contract is intentionally read-only,
/// uses the actual session identity for authority, and never returns runtime
/// secret values or raw infrastructure errors.
/// </summary>
public static class SystemArchitectureModule
{
    private const string ModuleNumber = "068";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";

    public static WebApplication MapSystemArchitectureEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/system-architecture/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);

        app.MapGet(
            "/api/system-architecture/dependency-status",
            (Func<HttpContext, Task<IResult>>)GetDependencyStatusAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);

        if (authorization.Failure is not null)
        {
            return authorization.Failure;
        }

        await using var connection = authorization.Connection!;
        var runtimeEnvironment = RuntimeEnvironment();

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "System Architecture & Dependency Map",
            status = "architecture_overview_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            runtimeRevision = RuntimeRevision(),
            generatedAt = DateTimeOffset.UtcNow,
            access = new
            {
                classification = "administrators_only",
                serverAuthorized = true,
                authoritySource = "actual_projectpulse_session",
                viewAsTransfersAuthority = false,
                isViewAs = IsViewAs(context)
            },
            scope = new
            {
                diagramType = "logical_runtime_architecture",
                environment = runtimeEnvironment,
                includes = new[]
                {
                    "component communication",
                    "data movement",
                    "authentication boundaries",
                    "external integrations",
                    "environment promotion",
                    "live status ownership"
                },
                excludes = new[]
                {
                    "secret values",
                    "tenant identifiers",
                    "database credentials",
                    "private host names",
                    "raw exception details",
                    "mutation controls"
                }
            },
            layers = ArchitectureLayers(),
            nodes = ArchitectureNodes(),
            connections = ArchitectureConnections(),
            trustBoundaries = TrustBoundaries(),
            environments = EnvironmentPath(),
            statusLinks = StatusLinks(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetDependencyStatusAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);

        if (authorization.Failure is not null)
        {
            return authorization.Failure;
        }

        await using var connection = authorization.Connection!;

        await using (var command = new NpgsqlCommand("SELECT 1;", connection))
        {
            await command.ExecuteScalarAsync();
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "dependency_status_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            observationMode = "safe_local_and_delegated_health",
            environment = RuntimeEnvironment(),
            dependencies = DependencyRows(),
            rules = new[]
            {
                "Only the authenticated session and database authorization query are checked directly by Module 068.",
                "Existing operational modules remain authoritative for service, integration, backup, restore, and deployment health.",
                "A delegated status is not represented as healthy until its owning live status center reports it.",
                "No secret value, raw provider response, host name, or connection string is returned."
            }
        });
    }

    private static ArchitectureLayer[] ArchitectureLayers() =>
    [
        new("experience", "Experience", 1,
            "Authenticated browser and ProjectPulse application shell"),
        new("delivery", "Web delivery", 2,
            "Static frontend delivery and API reverse-proxy boundary"),
        new("application", "Application services", 3,
            "ASP.NET Core API, module endpoints, authorization, and workflows"),
        new("data", "Data and evidence", 4,
            "PostgreSQL business records, audit evidence, and governed artifacts"),
        new("integration", "External integrations", 5,
            "Identity, business systems, mail, AI, and source-control providers"),
        new("operations", "Operations", 6,
            "Health, backup, restore, replication, build, release, and runtime controls")
    ];

    private static ArchitectureNode[] ArchitectureNodes() =>
    [
        new(
            "browser",
            "ProjectPulse user",
            "experience",
            "client",
            "Authenticated, role-scoped application access",
            ["renders authorized routes", "sends session context", "receives sanitized responses"]),
        new(
            "react-shell",
            "React application shell",
            "experience",
            "frontend",
            "Navigation, module composition, session-aware presentation",
            ["route visibility", "module mounting", "read-only architecture visualization"]),
        new(
            "web-runtime",
            "Web delivery runtime",
            "delivery",
            "runtime",
            "Serves built assets and forwards protected API requests",
            ["static assets", "HTTPS boundary", "API proxy"]),
        new(
            "projectpulse-api",
            "ProjectPulse API",
            "application",
            "backend",
            "Session validation, authorization, domain workflows, and integration adapters",
            ["actual/effective session separation", "role and permission enforcement", "sanitized API contracts"]),
        new(
            "postgresql",
            "ProjectPulse PostgreSQL",
            "data",
            "database",
            "Canonical application data and governed operational evidence",
            ["business records", "role assignments", "audit and workflow evidence"]),
        new(
            "artifact-storage",
            "Governed artifacts",
            "data",
            "storage",
            "Approved exports, backup evidence, and controlled runtime artifacts",
            ["retention controls", "restore evidence", "approved branded outputs"]),
        new(
            "identity-provider",
            "Microsoft identity and Graph",
            "integration",
            "identity",
            "Authentication routing and approved profile, presence, and calendar enrichment",
            ["OIDC/OAuth", "Module 062 identity profile", "Module 057 capacity signals"]),
        new(
            "business-integrations",
            "Business integrations",
            "integration",
            "integration",
            "SELL/CRM, Certinia, Certify, customer, rate, and related governed exchanges",
            ["bounded HTTPS contracts", "role-scoped data", "integration-owned status"]),
        new(
            "shared-platform-services",
            "Shared platform services",
            "integration",
            "platform",
            "Governed mail and AI provider abstractions used by approved feature modules",
            ["shared mail boundary", "shared AI routing boundary", "secret values remain server-side"]),
        new(
            "delivery-pipeline",
            "GitHub and OCI delivery pipeline",
            "operations",
            "delivery",
            "Source validation, immutable images, controlled promotion, and rollback evidence",
            ["GitHub Actions", "OCI registry", "workload identity"]),
        new(
            "operations-centers",
            "ProjectPulse operations centers",
            "operations",
            "monitoring",
            "Live service, API, backup, restore, replication, and deployment status ownership",
            ["Module 013", "Modules 014-017", "Module 058"])
    ];

    private static ArchitectureConnection[] ArchitectureConnections() =>
    [
        new("browser", "web-runtime", "HTTPS", "Application assets and navigation", "public_then_authenticated", "request_response"),
        new("react-shell", "projectpulse-api", "HTTPS/JSON", "Authorized module requests and responses", "authenticated", "request_response"),
        new("projectpulse-api", "postgresql", "PostgreSQL over protected network", "Business data and authorization evidence", "restricted", "request_response"),
        new("projectpulse-api", "artifact-storage", "Governed file access", "Approved artifacts and recovery evidence", "restricted", "request_response"),
        new("projectpulse-api", "identity-provider", "OIDC/OAuth/Graph HTTPS", "Authentication and approved identity enrichment", "restricted", "request_response"),
        new("projectpulse-api", "business-integrations", "Provider HTTPS contracts", "Approved operational and commercial records", "restricted", "request_response"),
        new("projectpulse-api", "shared-platform-services", "Server-side provider adapters", "Approved prompts, results, and outbound messages", "restricted", "request_response"),
        new("delivery-pipeline", "web-runtime", "OCI image promotion", "Versioned web release", "operational", "controlled_promotion"),
        new("delivery-pipeline", "projectpulse-api", "OCI image promotion", "Versioned API release", "operational", "controlled_promotion"),
        new("operations-centers", "projectpulse-api", "Protected internal API", "Sanitized health and evidence", "administrators_only", "read_only_observation")
    ];

    private static TrustBoundary[] TrustBoundaries() =>
    [
        new("browser-boundary", "Browser boundary", ["browser", "react-shell"],
            "The browser never receives provider secrets, database credentials, or privileged authority from View-As."),
        new("api-authorization-boundary", "API authorization boundary", ["projectpulse-api"],
            "Every protected endpoint validates the ProjectPulse session and enforces server-side roles or permissions."),
        new("data-boundary", "Data boundary", ["postgresql", "artifact-storage"],
            "Application data and evidence remain behind backend authorization and retention controls."),
        new("provider-boundary", "External provider boundary", ["identity-provider", "business-integrations", "shared-platform-services"],
            "Provider calls use approved server-side adapters, minimized data, sanitized failures, and governed configuration."),
        new("release-boundary", "Release boundary", ["delivery-pipeline", "web-runtime", "projectpulse-api"],
            "Source, validation, immutable artifacts, environment approval, and runtime verification remain distinct stages.")
    ];

    private static EnvironmentStage[] EnvironmentPath() =>
    [
        new("local", "Local development", 1, "Developer-owned runtime configuration", "No production secrets or production data assumed"),
        new("test", "Controlled test", 2, "Validated source and test-scoped runtime configuration", "Portal smoke tests and integration evidence"),
        new("production", "Production", 3, "Approved immutable release and production-scoped secret references", "Explicit approval, health validation, and rollback readiness")
    ];

    private static ArchitectureStatusLink[] StatusLinks() =>
    [
        new("service-control", "Service and API health", "#service-control", "/api/system/api-status", "Module 013", "live"),
        new("backup-dr", "Backup and disaster recovery", "#backup-dr", "/api/system/backup-dr/status", "Module 014", "live"),
        new("restore-validation", "Restore validation", "#restore-validation", "/api/system/restore-validation/status", "Module 015", "live"),
        new("backup-retention", "Backup retention", "#backup-retention", "/api/system/backup-retention/status", "Module 016", "live"),
        new("replication-sync", "Replication and synchronization", "#replication-sync", "/api/system/replication-sync/status", "Module 017", "live"),
        new("azure-admin", "Identity integration", "#azure-admin", "/api/admin/azure/config", "Module 010", "live"),
        new("cicd-pipeline", "Build and deployment", "#cicd-pipeline", "/api/cicd/status", "Module 058", "live")
    ];

    private static DependencyStatus[] DependencyRows() =>
    [
        new("projectpulse-session", "ProjectPulse session", "healthy", "direct", "Authenticated request accepted", null, null),
        new("postgresql", "ProjectPulse PostgreSQL", "healthy", "direct", "Authorization and SELECT 1 completed", "#service-control", "/api/system/api-status"),
        new("web-api-runtime", "Web and API runtime", "delegated", "live_status_owner", "Open Module 013 for current health", "#service-control", "/api/system/service-control/status"),
        new("identity", "Microsoft identity and Graph", "delegated", "live_status_owner", "Open Module 010 for current configuration and health", "#azure-admin", "/api/admin/azure/config"),
        new("backup-restore", "Backup and restore", "delegated", "live_status_owner", "Open Modules 014-016 for current evidence", "#backup-dr", "/api/system/backup-dr/status"),
        new("replication", "Replication and synchronization", "delegated", "live_status_owner", "Open Module 017 for current status", "#replication-sync", "/api/system/replication-sync/status"),
        new("delivery", "Source and delivery pipeline", "delegated", "live_status_owner", "Open Module 058 for current status", "#cicd-pipeline", "/api/cicd/status"),
        new("mail", "Shared outbound mail", "governed", "configuration_owner", "Module 067 is reserved for the future global mail configuration center", null, "/api/system/email-provider/summary"),
        new("ai", "Shared AI provider routing", "governed", "configuration_owner", "Module 064 package is parked pending the Module 002 integration checkpoint", null, null)
    ];

    private static string[] Guardrails() =>
    [
        "Module 068 exposes GET endpoints only and performs no data mutation.",
        "Actual-session administrator authority is required; View-As never grants architecture access.",
        "The diagram is logical and versioned. It does not perform network discovery or expose physical topology.",
        "Existing operational modules remain the source of truth for live health and recovery evidence.",
        "Secret values, connection strings, raw errors, private host names, and tenant identifiers are excluded.",
        "Module 059 remains globally mounted and every existing module route remains preserved."
    ];

    private static async Task<AuthorizationOutcome> OpenAuthorizedConnectionAsync(
        HttpContext context)
    {
        var actualUserId = ActualSessionUserId(context);

        if (actualUserId is null)
        {
            return new AuthorizationOutcome(
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
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "authorization_dependency_unavailable",
                    message = "System Architecture authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
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
                              'ADMINISTRATOR'
                          )
                          OR upper(COALESCE(p.permission_code, '')) IN (
                              'SYSTEM_ADMINISTRATION',
                              'MANAGE_ALL'
                          )
                      )
                );
                """, connection);

            command.Parameters.AddWithValue("user_id", actualUserId.Value);
            var allowed = Convert.ToBoolean(await command.ExecuteScalarAsync());

            if (!allowed)
            {
                await connection.DisposeAsync();

                return new AuthorizationOutcome(
                    null,
                    Results.Json(new
                    {
                        module = ModuleNumber,
                        status = "administrator_access_required",
                        message = "System Architecture is restricted to authorized administrators."
                    }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new AuthorizationOutcome(connection, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("SystemArchitectureModule");

            logger.LogWarning(
                "Module 068 authorization dependency unavailable ({ExceptionType}).",
                exception.GetType().Name);

            return new AuthorizationOutcome(
                null,
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "authorization_dependency_unavailable",
                    message = "System Architecture authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

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

    private static bool IsViewAs(HttpContext context)
    {
        return context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
            && value is bool isViewAs
            && isViewAs;
    }

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

        var builder = new NpgsqlConnectionStringBuilder
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
        };

        return builder.ConnectionString;
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

    private static string RuntimeRevision()
    {
        var value = (
            Environment.GetEnvironmentVariable("PROJECTPULSE_RELEASE_SHA")
            ?? Environment.GetEnvironmentVariable("SOURCE_COMMIT")
            ?? string.Empty).Trim();

        return value.Length is >= 7 and <= 40 && value.All(Uri.IsHexDigit)
            ? value.ToLowerInvariant()
            : "runtime_managed";
    }

    private sealed record AuthorizationOutcome(
        NpgsqlConnection? Connection,
        IResult? Failure);

    private sealed record ArchitectureLayer(
        string Id,
        string Name,
        int Order,
        string Description);

    private sealed record ArchitectureNode(
        string Id,
        string Name,
        string Layer,
        string Kind,
        string Description,
        string[] Responsibilities);

    private sealed record ArchitectureConnection(
        string From,
        string To,
        string Protocol,
        string Data,
        string Classification,
        string Direction);

    private sealed record TrustBoundary(
        string Id,
        string Name,
        string[] NodeIds,
        string Control);

    private sealed record EnvironmentStage(
        string Id,
        string Name,
        int Order,
        string Configuration,
        string Gate);

    private sealed record ArchitectureStatusLink(
        string Id,
        string Name,
        string Href,
        string ApiPath,
        string Owner,
        string Availability);

    private sealed record DependencyStatus(
        string Id,
        string Name,
        string State,
        string Observation,
        string Evidence,
        string? Href,
        string? ApiPath);
}
