using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 064 is the single sanitized configuration, health, and routing
/// boundary for every ProjectPulse AI consumer. Secret and persistence writes
/// remain locked until secure-store, step-up authentication, and audit work are
/// separately authorized.
/// </summary>
public static class AiProviderConfigurationModule
{
    private static readonly HashSet<string> AdministratorRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SUPER_ADMINISTRATOR",
            "SYSTEM_ADMINISTRATOR",
            "ADMINISTRATOR"
        };

    public static WebApplication MapAiProviderConfigurationEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/ai-configuration",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthRegistry, Task<IResult>>)GetConfigurationAsync);
        app.MapGet(
            "/api/ai-configuration/health",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthRegistry, Task<IResult>>)GetHealthAsync);
        app.MapPost(
            "/api/ai-configuration/health/refresh",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)RefreshHealthAsync);

        return app;
    }

    private static async Task<IResult> GetConfigurationAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthRegistry health)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        return Results.Ok(new
        {
            status = "configuration_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            configuration = configuration.ToSanitizedResponse(),
            health = health.Snapshots(),
            governance = GovernanceState()
        });
    }

    private static async Task<IResult> GetHealthAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthRegistry health)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        return Results.Ok(new
        {
            module = "064",
            status = OverallStatus(health.Snapshots()),
            generatedAt = DateTimeOffset.UtcNow,
            healthIntervalSeconds = configuration.HealthIntervalSeconds,
            requestTimeoutSeconds = configuration.RequestTimeoutSeconds,
            retryCount = configuration.RetryCount,
            maxOutputTokens = configuration.MaxOutputTokens,
            providers = health.Snapshots()
        });
    }

    private static async Task<IResult> RefreshHealthAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        var health = await coordinator.RefreshAsync(true, cancellationToken);
        return Results.Ok(new
        {
            module = "064",
            status = OverallStatus(health),
            message = "Configured provider health checks completed. Disabled or unconfigured providers were not contacted.",
            generatedAt = DateTimeOffset.UtcNow,
            healthIntervalSeconds = configuration.HealthIntervalSeconds,
            providers = health
        });
    }

    private static object GovernanceState() => new
    {
        sourcePhase = "full_shared_runtime_and_read_only_center",
        defaultPriority = new[]
        {
            ProjectPulseAiProviders.Claude,
            ProjectPulseAiProviders.OpenAi,
            ProjectPulseAiProviders.Local
        },
        providerAvailabilityChecked = true,
        unavailableProvidersSkipped = true,
        safetyRefusalFailover = false,
        secretValuesReturned = false,
        sharedRouterRequiredForAllConsumers = true,
        configurationMutation = "locked_pending_secure_store_authorization",
        secretRotation = "locked_pending_secure_store_authorization",
        activationAndRollback = "locked_pending_persistence_authorization",
        immutableAudit = "locked_pending_database_authorization",
        azureChanged = false,
        databaseChanged = false,
        entraChanged = false
    };

    private static string OverallStatus(IReadOnlyList<ProjectPulseAiProviderHealthSnapshot> health)
    {
        var remotes = health.Where(item =>
            !string.Equals(item.Provider, ProjectPulseAiProviders.Local, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (remotes.Any(item => item.Status == "available")) return "healthy";
        if (remotes.Any(item => item.Enabled && item.Configured)) return "degraded";
        return "local_fallback_only";
    }

    private static async Task<IResult?> AuthorizeAdministratorAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var connectionString = ConnectionString();
        if (connectionString is null)
        {
            return Results.Json(new
            {
                status = "configuration_unavailable",
                message = "Administrator authorization could not be verified."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT COALESCE(string_agg(DISTINCT r.role_code, ','), '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id
                   AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                WHERE u.user_id = @user_id
                  AND u.is_active = TRUE;
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roleText = (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
            var roles = roleText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (roles.Any(AdministratorRoles.Contains)) return null;
        }
        catch (Exception exception)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AiProviderConfigurationModule");
            logger.LogWarning(exception, "Module 064 could not verify administrator authorization.");

            return Results.Json(new
            {
                status = "authorization_unavailable",
                message = "Administrator authorization could not be verified."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(new
        {
            status = "access_denied",
            message = "AI Provider Configuration Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static string? ConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION",
            "PROJECTPULSE_DB_CONNECTION",
            "PROJECTTIME_DB_CONNECTION"
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }
}
