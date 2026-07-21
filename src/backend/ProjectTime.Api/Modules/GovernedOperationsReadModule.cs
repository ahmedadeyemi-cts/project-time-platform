using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class GovernedOperationsReadModule
{
    internal static async Task<IResult?> AuthorizeAsync(HttpContext context, string module, string[] roles, string[] permissions)
    {
        var userId = ActualUser(context);
        if (userId is null) return Results.Unauthorized();
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Json(new { module, code = "AUTHORIZATION_DEPENDENCY_UNAVAILABLE", message = "Module authorization is temporarily unavailable." }, statusCode: 503);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM app_user_role_assignments ura
                    JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                    LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                    LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                    WHERE ura.user_id = @user_id AND ura.is_active = TRUE
                      AND (upper(COALESCE(r.role_code, '')) = ANY(@roles)
                           OR upper(COALESCE(p.permission_code, '')) = ANY(@permissions))
                );
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            command.Parameters.AddWithValue("roles", roles);
            command.Parameters.AddWithValue("permissions", permissions);
            if (await command.ExecuteScalarAsync() is true) return null;
            return Results.Json(new { module, code = "MODULE_ACCESS_REQUIRED", message = "Your account is not authorized for this operational center." }, statusCode: 403);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GovernedOperationsReadModule")
                .LogWarning("Module {Module} authorization unavailable ({ExceptionType}).", module, exception.GetType().Name);
            return Results.Json(new { module, code = "AUTHORIZATION_DEPENDENCY_UNAVAILABLE", message = "Module authorization is temporarily unavailable." }, statusCode: 503);
        }
    }

    internal static object Surface(string module, string version, string surface, string title, string description, string action, object[] entries) => new
    {
        module, contractVersion = version, surface, title, description, action,
        liveData = entries.Length > 0,
        entries,
        generatedAt = DateTimeOffset.UtcNow
    };

    internal static object OperationalSurface(string module, string version, string surface)
    {
        var content = (module, surface) switch
        {
            ("077", "overview") => ("Release readiness", "Governed releases, environments, gates, evidence, and rollback.", "Prepare release", new object[] { new { name = "Current ProjectPulse deployment", status = "Deployed", authority = "GitHub Actions and Azure" } }),
            ("077", "releases") => ("Release inventory", "Immutable builds and their promotion state.", "Prepare release", Array.Empty<object>()),
            ("077", "environments") => ("Environments", "Promotion targets and current verification state.", "Review environments", new object[] { new { name = "Test", status = "Active", promotion = "CI-gated" } }),
            ("077", "gates") => ("Deployment gates", "Required checks before promotion.", "Evaluate gates", new object[] { new { name = "API Release build", required = true }, new { name = "Frontend production build", required = true }, new { name = "Protected validators", required = true } }),
            ("077", "evidence") => ("Release evidence", "Commit, workflow, artifact, deployment, and UAT evidence.", "View evidence", Array.Empty<object>()),
            ("077", _) => ("Rollback policy", "Controlled restoration to a verified revision.", "Review policy", new object[] { new { rule = "Rollback requires known-good revision and verification", status = "Enforced" } }),
            ("078", "overview") => ("Application health", "ProjectPulse service, dependency, signal, and SLO view.", "Add SLO", new object[] { new { service = "ProjectPulse API", observation = "This request succeeded", status = "Reachable" } }),
            ("078", "services") => ("Service catalog", "Owned services and dependency relationships.", "Register service", new object[] { new { service = "ProjectPulse API", owner = "Platform Operations", tier = "Critical" }, new { service = "ProjectPulse Web", owner = "Platform Operations", tier = "Critical" } }),
            ("078", "signals") => ("Signals", "Availability, latency, errors, saturation, and dependency health.", "Connect telemetry", Array.Empty<object>()),
            ("078", "slos") => ("SLOs and error budgets", "Measurable reliability objectives and remaining budget.", "Create SLO", Array.Empty<object>()),
            ("078", "alerts") => ("Alert history", "Actionable threshold breaches and ownership.", "Configure alert", Array.Empty<object>()),
            ("078", "integrations") => ("Telemetry integrations", "Approved sources for platform signals.", "Connect source", Array.Empty<object>()),
            ("078", _) => ("Retention policy", "Telemetry is retained only for an approved purpose and duration.", "Review policy", new object[] { new { classification = "Operational telemetry", secretValues = "Excluded", customerPayloads = "Excluded" } }),
            ("079", "overview") => ("Governance posture", "Data ownership, classification, lineage, retention, holds, and privacy.", "Register domain", new object[] { new { domain = "ProjectPulse operational data", owner = "Platform Administration", status = "Governed" } }),
            ("079", "domains") => ("Data domains", "Accountable owners and stewardship boundaries.", "Register domain", new object[] { new { domain = "Identity and access", classification = "Restricted", owner = "Security Administration" }, new { domain = "Project delivery", classification = "Confidential", owner = "Delivery Operations" } }),
            ("079", "classifications") => ("Classifications", "Handling requirements by sensitivity.", "Review classifications", new object[] { new { level = "Restricted", handling = "Least privilege; no unredacted export" }, new { level = "Confidential", handling = "Authorized business use" }, new { level = "Internal", handling = "Authenticated users" } }),
            ("079", "retention-policies") => ("Retention policies", "Purpose-bound retention and defensible disposition.", "Create policy", Array.Empty<object>()),
            ("079", "lineage") => ("Data lineage", "Source, transformation, storage, and consumers.", "Register lineage", Array.Empty<object>()),
            ("079", "legal-holds") => ("Legal holds", "Preservation overrides that prevent disposition.", "Create hold", Array.Empty<object>()),
            ("079", _) => ("Privacy policy", "Access, correction, export, and deletion requests require verification.", "Review policy", new object[] { new { control = "Verified requester and scoped records", status = "Required" }, new { control = "Legal hold and retention eligibility check", status = "Required" } }),
            ("080", "overview") => ("Delivery acceptance", "Customer engagements, deliverables, reviews, and evidence.", "Start engagement", new object[] { new { workflow = "Draft → In review → Accepted or rejected", evidence = "Retained" } }),
            ("080", "engagements") => ("Engagements", "Customer-scoped delivery workspaces.", "Start engagement", Array.Empty<object>()),
            ("080", "milestones") => ("Milestones", "Delivery dates, owners, dependencies, and status.", "Add milestone", Array.Empty<object>()),
            ("080", "artifacts") => ("Deliverables", "Versioned artifacts and review readiness.", "Add deliverable", Array.Empty<object>()),
            ("080", "reviews") => ("Review queue", "Comments, decisions, decision makers, and timestamps.", "Request review", Array.Empty<object>()),
            ("080", "acceptance-policy") => ("Acceptance policy", "Explicit acceptance criteria and immutable decision evidence.", "Review policy", new object[] { new { control = "Named authorized approver", status = "Required" }, new { control = "Acceptance criteria and version", status = "Required" } }),
            ("080", _) => ("Sharing policy", "Customer sharing is scoped, expiring, revocable, and audited.", "Review policy", new object[] { new { control = "External sharing connector", status = "Not configured" } }),
            _ => ("Operational surface", "Governed module information.", "Review", Array.Empty<object>())
        };
        return Surface(module, version, surface, content.Item1, content.Item2, content.Item3, content.Item4);
    }

    internal static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    internal static bool HasActualUser(HttpContext context) => ActualUser(context) is not null;

    private static Guid? ActualUser(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        return new NpgsqlConnectionStringBuilder { Host = host, Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432, Database = database, Username = username, Password = password, IncludeErrorDetail = false, Pooling = true, MaxPoolSize = 5 }.ConnectionString;
    }
}
