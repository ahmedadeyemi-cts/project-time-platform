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
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, CancellationToken, Task<IResult>>)GetConfigurationAsync);
        app.MapGet(
            "/api/ai-configuration/health",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthRegistry, Task<IResult>>)GetHealthAsync);
        app.MapPost(
            "/api/ai-configuration/health/refresh",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)RefreshHealthAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/secret",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, CancellationToken, Task<IResult>>)ReplaceSecretAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/model",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)ReplaceModelAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/enabled",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)SetEnabledAsync);

        return app;
    }

    private static async Task<IResult> ReplaceModelAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry healthRegistry,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: 403);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode is not (ProjectPulseAiProviders.Claude or ProjectPulseAiProviders.OpenAi))
            return Results.BadRequest(new { status = "invalid_provider", message = "Provider must be claude or openai." });

        ReplaceModelRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<ReplaceModelRequest>(cancellationToken); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." }); }
        var model = request?.Model?.Trim();
        var current = configuration.Provider(providerCode);
        if (string.IsNullOrWhiteSpace(model) || !current.ApprovedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "model_not_approved", message = "Select a model from the approved list." });
        if (!current.Configured)
            return Results.BadRequest(new { status = "provider_not_configured", message = "Save the provider API key before changing its model." });

        var previousModel = current.Model;
        try
        {
            await store.SaveModelAsync(providerCode, model, ActualSessionUserId(context)!.Value, cancellationToken);
            configuration.ApplyStoredModel(providerCode, model);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
            var snapshots = await coordinator.RefreshAsync(true, cancellationToken);
            var probe = snapshots.First(item => string.Equals(item.Provider, providerCode, StringComparison.OrdinalIgnoreCase));
            if (probe.Status != "available")
            {
                await store.SaveModelAsync(providerCode, previousModel, ActualSessionUserId(context)!.Value, cancellationToken);
                configuration.ApplyStoredModel(providerCode, previousModel);
                healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
                return Results.BadRequest(new
                {
                    status = "model_test_failed",
                    message = $"{model} could not be verified with the saved key. The previous model remains active.",
                    activeModel = previousModel
                });
            }

            return Results.Ok(new
            {
                status = "model_changed",
                provider = providerCode,
                model,
                tested = true,
                message = $"{model} was verified and is now active."
            });
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to change the {Provider} model.", providerCode);
            return Results.Json(new { status = "model_change_error", message = "The model could not be saved and tested." }, statusCode: 503);
        }
    }

    private static async Task<IResult> SetEnabledAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry healthRegistry,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: 403);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode is not (ProjectPulseAiProviders.Claude or ProjectPulseAiProviders.OpenAi))
            return Results.BadRequest(new { status = "invalid_provider", message = "Provider must be claude or openai." });
        SetEnabledRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<SetEnabledRequest>(cancellationToken); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." }); }
        var enabled = request?.Enabled;
        if (!enabled.HasValue) return Results.BadRequest(new { status = "invalid_request", message = "Enabled must be true or false." });

        var provider = configuration.Provider(providerCode);
        if (enabled.Value && !provider.Configured)
            return Results.BadRequest(new { status = "provider_not_configured", message = "Save an API key before enabling this provider." });
        try
        {
            await store.SaveEnabledAsync(providerCode, enabled.Value, provider.Model, ActualSessionUserId(context)!.Value, cancellationToken);
            configuration.ApplyStoredEnabled(providerCode, enabled.Value);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
            if (enabled.Value) await coordinator.RefreshAsync(true, cancellationToken);
            return Results.Ok(new
            {
                status = enabled.Value ? "provider_enabled" : "provider_disabled",
                provider = providerCode,
                enabled = enabled.Value,
                message = $"{provider.DisplayName} is now {(enabled.Value ? "enabled" : "disabled")}. The saved key and model were preserved."
            });
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to change the {Provider} enabled state.", providerCode);
            return Results.Json(new { status = "provider_state_error", message = "The provider state could not be changed." }, statusCode: 503);
        }
    }

    private static async Task<IResult> ReplaceSecretAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore secretStore,
        ProjectPulseAiHealthRegistry healthRegistry,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: 403);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode is not (ProjectPulseAiProviders.Claude or ProjectPulseAiProviders.OpenAi))
            return Results.BadRequest(new { status = "invalid_provider", message = "Provider must be claude or openai." });
        if (!secretStore.Available)
            return Results.Json(new { status = "secure_store_unavailable", message = secretStore.UnavailableReason }, statusCode: 503);

        ReplaceSecretRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<ReplaceSecretRequest>(cancellationToken); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." }); }
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return Results.BadRequest(new { status = "invalid_secret", message = "API key is required." });
        if (apiKey.Any(char.IsWhiteSpace)) return Results.BadRequest(new { status = "invalid_secret", message = "API key cannot contain whitespace." });

        try
        {
            var stored = await secretStore.SaveAsync(providerCode, apiKey, ActualSessionUserId(context)!.Value, cancellationToken);
            configuration.ApplyStoredSecret(stored.ProviderCode, stored.ApiKey, stored.Version, stored.RotatedAt);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
            return Results.Ok(new
            {
                status = "secret_replaced",
                provider = providerCode,
                configured = true,
                version = stored.Version,
                rotatedAt = stored.RotatedAt,
                valueReturned = false,
                message = $"{configuration.Provider(providerCode).DisplayName} API key was saved securely. The value cannot be viewed after saving."
            });
        }
        catch (ArgumentException exception) { return Results.BadRequest(new { status = "invalid_secret", message = exception.Message }); }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to replace the {Provider} secret.", providerCode);
            return Results.Json(new { status = "secret_store_error", message = "The API key could not be saved securely." }, statusCode: 503);
        }
    }

    private static async Task<IResult> GetConfigurationAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry health,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (store.Available)
        {
            foreach (var secret in await store.LoadAsync(cancellationToken))
            {
                configuration.ApplyStoredSecret(secret.ProviderCode, secret.ApiKey, secret.Version, secret.RotatedAt);
                health.ApplyConfiguration(configuration.Provider(secret.ProviderCode));
            }
            foreach (var setting in await store.LoadModelsAsync(cancellationToken))
            {
                configuration.ApplyStoredModel(setting.Key, setting.Value);
                health.ApplyConfiguration(configuration.Provider(setting.Key));
            }
            foreach (var setting in await store.LoadEnabledAsync(cancellationToken))
            {
                configuration.ApplyStoredEnabled(setting.Key, setting.Value);
                health.ApplyConfiguration(configuration.Provider(setting.Key));
            }
        }

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
        configurationMutation = "administrator_write_only_secret_replacement",
        secretRotation = "replace_in_place_with_encrypted_version",
        activationAndRollback = "replacement_active_immediately_rollback_not_exposed",
        immutableAudit = "sanitized_database_audit_enabled",
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

    private static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;

        // Sec-Fetch-Site is a forbidden browser-controlled header. For same-origin
        // requests it remains reliable even when the web reverse proxy replaces Host
        // with the API container's internal hostname.
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)) return true;

        // Preserve exact host validation for clients and deployments that do not send
        // Fetch Metadata headers. Prefer the original public host supplied by the
        // trusted reverse proxy, then fall back to the request host.
        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var publicHost = !string.IsNullOrWhiteSpace(forwardedHost)
            ? HostString.FromUriComponent(forwardedHost)
            : context.Request.Host;

        if (!string.Equals(uri.Host, publicHost.Host, StringComparison.OrdinalIgnoreCase)) return false;
        return publicHost.Port is null || uri.Port == publicHost.Port;
    }

    private sealed record ReplaceSecretRequest(string? ApiKey);
    private sealed record ReplaceModelRequest(string? Model);
    private sealed record SetEnabledRequest(bool? Enabled);

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
